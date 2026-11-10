using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read model in SQL, untracked
internal sealed class ClearanceQueries(DContext db) : IClearanceQueries
{
    // Scoped to the organization: a policyId of another organization gives an empty list
    public async Task<IReadOnlyList<ClearanceEntry>> ListAsync(
        Guid organizationId,
        Guid? userId,
        Guid? policyId,
        CancellationToken cancellationToken) =>
        await OnPoliciesOf(organizationId)
            .Where(clearance => userId == null || clearance.UserId == userId)
            .Where(clearance => policyId == null || clearance.PolicyId == policyId)
            .OrderBy(clearance => clearance.Name)
            .Select(clearance => new ClearanceEntry(
                clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId,
                clearance.ClassificationLevel.Name, clearance.ClassificationLevel.Priority))
            .ToListAsync(cancellationToken);

    // Scoped to the organization: a clearance on a policy of another organization is not found
    public Task<ClearanceEntry?> FindAsync(
        Guid organizationId,
        Guid clearanceId,
        CancellationToken cancellationToken) =>
        OnPoliciesOf(organizationId)
            .Where(clearance => clearance.Id == clearanceId)
            .Select(clearance => new ClearanceEntry(
                clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId,
                clearance.ClassificationLevel.Name, clearance.ClassificationLevel.Priority))
            .FirstOrDefaultAsync(cancellationToken);

    // The clearances on the policies of the organization, untracked
    private IQueryable<DbClearance> OnPoliciesOf(Guid organizationId) =>
        db.Clearances
            .AsNoTracking()
            .Where(clearance => db.Policies.Any(
                policy => policy.Id == clearance.PolicyId && policy.OrganizationId == organizationId));
}
