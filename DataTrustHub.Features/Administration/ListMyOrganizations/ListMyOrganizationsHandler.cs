using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListMyOrganizations;

public record ListMyOrganizationsQuery(Guid CallerId);

// A user lists the organizations they belong to, with their membership role in each (ADR-0014)
public class ListMyOrganizationsHandler(DContext db)
    : IQueryHandler<ListMyOrganizationsQuery, Result<IReadOnlyList<MyOrganizationSummary>>>
{
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<MyOrganizationSummary>>> Handle(
        ListMyOrganizationsQuery query,
        CancellationToken cancellationToken)
    {
        // The inner join leaves out a membership whose organization no longer exists
        var organizations = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == query.CallerId)
            .Join(_db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { Membership = m, o.Name })
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Membership.OrganizationId)
            .Select(row => new MyOrganizationSummary(
                row.Membership.OrganizationId, row.Name, row.Membership.Id, row.Membership.Role))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MyOrganizationSummary>>(organizations);
    }
}
