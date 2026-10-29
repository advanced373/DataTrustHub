using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListClearances;

public record ListClearancesQuery(Guid OrganizationId, Guid CallerId, Guid? UserId, Guid? PolicyId);

// An organization admin lists the clearances on the policies of their organization (C6)
public class ListClearancesHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<ListClearancesQuery, Result<IReadOnlyList<ClearanceSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<ClearanceSummary>>> Handle(
        ListClearancesQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<ClearanceSummary>>(Errors.OrganizationNotFound);

        // Scoped to the organization: a policyId of another organization gives an empty list
        var clearances = await _db.Clearances
            .AsNoTracking()
            .Where(c => _db.Policies.Any(p => p.Id == c.PolicyId && p.OrganizationId == query.OrganizationId))
            .Where(c => query.UserId == null || c.UserId == query.UserId)
            .Where(c => query.PolicyId == null || c.PolicyId == query.PolicyId)
            .OrderBy(c => c.Name)
            .Select(c => new ClearanceSummary(
                c.Id, c.Name, c.UserId, c.PolicyId, c.ClassificationLevel.Name, c.ClassificationLevel.Priority))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ClearanceSummary>>(clearances);
    }
}
