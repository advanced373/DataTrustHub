using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetPolicy;

public record GetPolicyQuery(Guid OrganizationId, Guid CallerId, Guid PolicyId);

// An organization admin reads a policy of their organization (C6)
public class GetPolicyHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<GetPolicyQuery, Result<GetPolicyResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<GetPolicyResponse>> Handle(
        GetPolicyQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<GetPolicyResponse>(Errors.OrganizationNotFound);

        // Scoped to the organization: a policy of another organization is not found
        var policy = await _db.Policies
            .AsNoTracking()
            .Where(p => p.Id == query.PolicyId && p.OrganizationId == query.OrganizationId)
            .Select(p => new GetPolicyResponse(p.Id, p.Name, p.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

        return policy is null
            ? Result.Failure<GetPolicyResponse>(Errors.PolicyNotFound)
            : Result.Success(policy);
    }
}
