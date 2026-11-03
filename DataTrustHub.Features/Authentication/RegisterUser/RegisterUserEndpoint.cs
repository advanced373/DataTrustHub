using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.RateLimiting;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserRequest(string Email, string Password);
public record RegisterUserResponse(Guid RegistrationRequestId);

// Register creates a registration request, not an account: it answers 202 and the account waits for
// approval (ADR-0026). Limited per client address
public class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .AllowAnonymous()
           .RequireRateLimiting(RateLimitPolicies.Registration);

    private static async Task<IResult> Handle(
        RegisterUserRequest request,
        ICommandHandler<RegisterUserCommand, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(request.Email, request.Password);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(registrationRequestId =>
            Results.Accepted(value: new RegisterUserResponse(registrationRequestId)));
    }
}
