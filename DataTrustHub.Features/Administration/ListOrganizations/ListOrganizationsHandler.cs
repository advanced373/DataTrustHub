using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListOrganizations;

public record ListOrganizationsQuery;

public class ListOrganizationsHandler(DContext db)
    : IQueryHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationSummary>>>
{
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<OrganizationSummary>>> Handle(
        ListOrganizationsQuery query,
        CancellationToken cancellationToken)
    {
        var organizations = await _db.Organizations
            .AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationSummary(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<OrganizationSummary>>(organizations);
    }
}
