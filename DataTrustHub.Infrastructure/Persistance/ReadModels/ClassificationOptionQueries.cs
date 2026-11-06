using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Groups the user's clearances by policy in SQL, untracked
internal sealed class ClassificationOptionQueries(DContext db) : IClassificationOptionQueries
{
    // The grouped rows are few; they are ordered in memory, so the order does not depend on the database collation
    public async Task<IReadOnlyList<ClearedPolicy>> ListClearedPoliciesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var policies = await (
                from clearance in db.Clearances.AsNoTracking()
                where clearance.UserId == userId
                join policy in db.Policies on clearance.PolicyId equals policy.Id
                join organization in db.Organizations on policy.OrganizationId equals organization.Id
                group clearance.ClassificationLevel.Priority
                    by new { policy.Id, policy.Name, OrganizationName = organization.Name } into byPolicy
                select new ClearedPolicy(
                    byPolicy.Key.Id, byPolicy.Key.Name, byPolicy.Key.OrganizationName, byPolicy.Max()))
            .ToListAsync(cancellationToken);

        return policies.OrderBy(p => p.OrganizationName).ThenBy(p => p.PolicyName).ToList();
    }
}
