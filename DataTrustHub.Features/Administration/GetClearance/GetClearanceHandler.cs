using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetClearance;

public record GetClearanceQuery(Guid OrganizationId, Guid CallerId, Guid ClearanceId);

// An organization admin reads a clearance on a policy of their organization (C6)
public class GetClearanceHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<GetClearanceQuery, Result<GetClearanceResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<GetClearanceResponse>> Handle(
        GetClearanceQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<GetClearanceResponse>(Errors.OrganizationNotFound);

        // Scoped to the organization: a clearance on a policy of another organization is not found
        var clearance = await _db.Clearances
            .AsNoTracking()
            .Where(c => c.Id == query.ClearanceId)
            .Where(c => _db.Policies.Any(p => p.Id == c.PolicyId && p.OrganizationId == query.OrganizationId))
            .Select(c => new GetClearanceResponse(
                c.Id, c.Name, c.UserId, c.PolicyId, c.ClassificationLevel.Name, c.ClassificationLevel.Priority))
            .FirstOrDefaultAsync(cancellationToken);

        return clearance is null
            ? Result.Failure<GetClearanceResponse>(Errors.ClearanceNotFound)
            : Result.Success(clearance);
    }
}
