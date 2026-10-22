using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetOrganization;

public record GetOrganizationQuery(Guid OrganizationId);

public class GetOrganizationHandler(DContext db) : IQueryHandler<GetOrganizationQuery, Result<GetOrganizationResponse>>
{
    private readonly DContext _db = db;

    public async Task<Result<GetOrganizationResponse>> Handle(
        GetOrganizationQuery query,
        CancellationToken cancellationToken)
    {
        var organization = await _db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == query.OrganizationId)
            .Select(o => new GetOrganizationResponse(o.Id, o.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return organization is null
            ? Result.Failure<GetOrganizationResponse>(Errors.OrganizationNotFound)
            : Result.Success(organization);
    }
}
