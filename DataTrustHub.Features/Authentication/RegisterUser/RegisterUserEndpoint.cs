using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserRequest(string Email, string Password);
public record RegisterUserResponse(Guid UserId);

public class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .AllowAnonymous();

    private static async Task<IResult> Handle(
        RegisterUserRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(request.Email, request.Password);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(userId => Results.Ok(new RegisterUserResponse(userId)));
    }
}
