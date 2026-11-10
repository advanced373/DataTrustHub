using DataTrustHub.Domain.ReadModels.Policies;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read model in SQL, untracked
internal sealed class PolicyQueries(DContext db) : IPolicyQueries
{
    public async Task<IReadOnlyList<PolicyEntry>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await db.Policies
            .AsNoTracking()
            .Where(policy => policy.OrganizationId == organizationId)
            .OrderBy(policy => policy.Name)
            .Select(policy => new PolicyEntry(policy.Id, policy.Name, policy.OrganizationId))
            .ToListAsync(cancellationToken);

    // Scoped to the organization: a policy of another organization is not found
    public Task<PolicyEntry?> FindAsync(Guid organizationId, Guid policyId, CancellationToken cancellationToken) =>
        db.Policies
            .AsNoTracking()
            .Where(policy => policy.Id == policyId && policy.OrganizationId == organizationId)
            .Select(policy => new PolicyEntry(policy.Id, policy.Name, policy.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);
}
