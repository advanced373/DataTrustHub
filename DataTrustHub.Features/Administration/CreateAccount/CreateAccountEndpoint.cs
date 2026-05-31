using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.CreateAccount;

public record CreateAccountRequest(string Email, string Password);
public record CreateAccountResponse(Guid UserId);

public class CreateAccountEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole("Admin"));

    private static async Task<IResult> Handle(
        CreateAccountRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateAccountCommand(request.Email, request.Password);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(userId => Results.Ok(new CreateAccountResponse(userId)));
    }
}
