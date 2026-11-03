using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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
        ICommandHandler<AddDataItemCommand, Result<Guid>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userIdResult = httpContext.User.GetUserId();
        if (userIdResult is null) return Results.Unauthorized();

        var command = new AddDataItemCommand(file, userIdResult.Value, policyId, levelPriority);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(id => Results.Ok(new AddDataItemResponse(id)));
    }
}
