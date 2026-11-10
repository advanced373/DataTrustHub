using DataTrustHub.Domain.ReadModels.Organizations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read model in SQL, untracked
internal sealed class OrganizationQueries(DContext db) : IOrganizationQueries
{
    public async Task<IReadOnlyList<OrganizationEntry>> ListAsync(CancellationToken cancellationToken) =>
        await db.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.Name)
            .Select(organization => new OrganizationEntry(organization.Id, organization.Name))
            .ToListAsync(cancellationToken);

    public Task<OrganizationEntry?> FindAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationEntry(organization.Id, organization.Name))
            .FirstOrDefaultAsync(cancellationToken);
}
