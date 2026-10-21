using DataTrustHub.Domain.Organizations;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

internal sealed class OrganizationRepository(DContext db) : IOrganizationRepository
{
    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken))?.ToDomain();

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organizations.AnyAsync(o => o.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        db.Organizations.AnyAsync(o => o.Name == name, cancellationToken);

    public void Add(Organization organization) => db.Organizations.Add(organization.ToDb());

    public async Task RemoveAsync(Organization organization, CancellationToken cancellationToken) =>
        db.Organizations.Remove(await db.Organizations.FirstAsync(o => o.Id == organization.Id, cancellationToken));
}
