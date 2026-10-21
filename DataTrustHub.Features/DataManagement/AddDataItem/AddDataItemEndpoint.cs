using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

public record AddDataItemResponse(Guid DataItemId);

public class AddDataItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization()
           .DisableAntiforgery()
           // Kestrel defaults to 30 MB per request; allow the full file size plus multipart overhead
           .WithMetadata(new RequestSizeLimitAttribute(Constants.MaxRequestSizeBytes))
           .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = Constants.MaxRequestSizeBytes });

    private static async Task<IResult> Handle(
        IFormFile? file,
        [FromForm] Guid? policyId,
        [FromForm] int? levelPriority,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userIdResult = ExtractUserId(httpContext.User);
        if (userIdResult is null) return Results.Unauthorized();

        var command = new AddDataItemCommand(file, userIdResult.Value, policyId, levelPriority);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(id => Results.Ok(new AddDataItemResponse(id)));
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
