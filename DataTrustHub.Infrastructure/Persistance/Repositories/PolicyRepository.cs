using DataTrustHub.Domain.Policies;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

internal sealed class PolicyRepository(DContext db) : IPolicyRepository
{
    public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken))?.ToDomain();

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Policies.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<bool> NameExistsInOrganizationAsync(string name, Guid organizationId, CancellationToken cancellationToken) =>
        db.Policies.AnyAsync(p => p.Name == name && p.OrganizationId == organizationId, cancellationToken);

    public Task<bool> AnyInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Policies.AnyAsync(p => p.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Policies.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

    public void Add(Policy policy)
    {
        var row = policy.ToDb();
        row.ClassificationLevels = [];
        db.Policies.Add(row);
    }

    public async Task RemoveAsync(Policy policy, CancellationToken cancellationToken) =>
        db.Policies.Remove(await db.Policies.FirstAsync(p => p.Id == policy.Id, cancellationToken));
}
