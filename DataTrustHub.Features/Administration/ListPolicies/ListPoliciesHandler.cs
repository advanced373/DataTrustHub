using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListPolicies;

public record ListPoliciesQuery(Guid OrganizationId, Guid CallerId);

// An organization admin lists the policies of their organization (C6)
public class ListPoliciesHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<ListPoliciesQuery, Result<IReadOnlyList<PolicySummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<PolicySummary>>> Handle(
        ListPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<PolicySummary>>(Errors.OrganizationNotFound);

        var policies = await _db.Policies
            .AsNoTracking()
            .Where(p => p.OrganizationId == query.OrganizationId)
            .OrderBy(p => p.Name)
            .Select(p => new PolicySummary(p.Id, p.Name, p.OrganizationId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PolicySummary>>(policies);
    }
}
