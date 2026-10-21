using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListPolicies;

public record ListPoliciesQuery(Guid? OrganizationId);

public class ListPoliciesHandler(DContext db)
    : IQueryHandler<ListPoliciesQuery, Result<IReadOnlyList<PolicySummary>>>
{
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<PolicySummary>>> Handle(
        ListPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        var policies = await _db.Policies
            .AsNoTracking()
            .Where(p => query.OrganizationId == null || p.OrganizationId == query.OrganizationId)
            .OrderBy(p => p.Name)
            .Select(p => new PolicySummary(p.Id, p.Name, p.OrganizationId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PolicySummary>>(policies);
    }
}
