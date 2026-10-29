using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.DataManagement.ListDataItems;

// Policy fields are null for a personal item; CanDelete tells the client whether the requester may delete
// the item (D5'), IsOwner whether they uploaded it
public record DataItemSummary(
    Guid Id,
    string Name,
    long Size,
    string SecurityMarking,
    int? ClassificationPriority,
    Guid? PolicyId,
    string? PolicyName,
    string? OwnerEmail,
    bool IsOwner,
    bool CanDelete);

public class ListDataItemsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        IQueryHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requesterId = ExtractUserId(httpContext.User);
        if (requesterId is null) return Results.Unauthorized();

        var result = await handler.Handle(new ListDataItemsQuery(requesterId.Value), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
