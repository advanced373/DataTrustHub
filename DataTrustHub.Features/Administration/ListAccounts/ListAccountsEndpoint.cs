using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListAccounts;

public record AccountSummary(Guid Id, string Email, string Role);

public class ListAccountsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(IQueryHandler<ListAccountsQuery, Result<IReadOnlyList<AccountSummary>>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListAccountsQuery(), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
