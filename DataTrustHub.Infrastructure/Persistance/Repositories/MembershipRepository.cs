using DataTrustHub.Domain.Memberships;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

internal sealed class MembershipRepository(DContext db) : IMembershipRepository
{
    public async Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken))?.ToDomain();

    public async Task<Membership?> GetAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        (await db.Memberships.AsNoTracking().FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken))?.ToDomain();

    public Task<bool> ExistsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);

    public Task<int> CountOrgAdminsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var orgAdminName = MembershipRole.OrgAdmin.Name;
        return db.Memberships.CountAsync(
            m => m.OrganizationId == organizationId && m.Role == orgAdminName, cancellationToken);
    }

    public Task<IReadOnlyList<Membership>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        ListAsync(m => m.OrganizationId == organizationId, cancellationToken);

    public Task<IReadOnlyList<Membership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ListAsync(m => m.UserId == userId, cancellationToken);

    public void Add(Membership membership) => db.Memberships.Add(membership.ToDb());

    public async Task UpdateAsync(Membership membership, CancellationToken cancellationToken)
    {
        var row = await db.Memberships.FirstAsync(m => m.Id == membership.Id, cancellationToken);
        membership.CopyTo(row);
    }

    public async Task RemoveAsync(Membership membership, CancellationToken cancellationToken) =>
        db.Memberships.Remove(await db.Memberships.FirstAsync(m => m.Id == membership.Id, cancellationToken));

    private async Task<IReadOnlyList<Membership>> ListAsync(
        Expression<Func<DbMembership, bool>> filter, CancellationToken cancellationToken)
    {
        var rows = await db.Memberships.AsNoTracking().Where(filter).ToListAsync(cancellationToken);
        return rows.Select(row => row.ToDomain()).ToList();
    }
}
