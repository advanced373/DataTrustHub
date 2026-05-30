using Carter;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserRequest(string Email, string Password);
public record LoginUserResponse(string Token);

public class LoginUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .AllowAnonymous();

    private static async Task<IResult> Handle(
        LoginUserRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new LoginUserCommand(request.Email, request.Password);
        var result = await sender.Send(command, cancellationToken);

        if (result.IsSuccess) return Results.Ok(new LoginUserResponse(result.Value));

        // Return 400 for validation errors, 401 for auth failures
        // Do not reveal whether email or password is wrong on auth failure
        if (result.Error.Type == ErrorType.Validation)
            return Results.BadRequest(result.Error);

        return Results.Unauthorized();
    }
}
