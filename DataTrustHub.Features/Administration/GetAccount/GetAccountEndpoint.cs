using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.GetAccount;

public record GetAccountResponse(Guid Id, string Email, string Role);

public class GetAccountEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(Guid id, IQueryHandler<GetAccountQuery, Result<GetAccountResponse>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetAccountQuery(id), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
