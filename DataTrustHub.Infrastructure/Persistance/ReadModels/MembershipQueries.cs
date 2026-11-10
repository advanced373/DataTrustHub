using DataTrustHub.Domain.ReadModels.Memberships;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read models in SQL, untracked
internal sealed class MembershipQueries(DContext db) : IMembershipQueries
{
    public async Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await db.Memberships
            .AsNoTracking()
            .Where(membership => membership.OrganizationId == organizationId)
            .Join(db.Users, membership => membership.UserId, user => user.Id,
                (membership, user) => new { Membership = membership, user.Email })
            .OrderBy(row => row.Email)
            .Select(row => new OrganizationMember(
                row.Membership.Id, row.Membership.UserId, row.Email, row.Membership.Role))
            .ToListAsync(cancellationToken);

    // The inner join leaves out a membership whose organization no longer exists
    public async Task<IReadOnlyList<MyOrganization>> ListMyOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(db.Organizations, membership => membership.OrganizationId, organization => organization.Id,
                (membership, organization) => new { Membership = membership, organization.Name })
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Membership.OrganizationId)
            .Select(row => new MyOrganization(
                row.Membership.OrganizationId, row.Name, row.Membership.Id, row.Membership.Role))
            .ToListAsync(cancellationToken);

    public Task<string?> FindRoleAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        db.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.OrganizationId == organizationId)
            .Select(membership => membership.Role)
            .FirstOrDefaultAsync(cancellationToken);
}
