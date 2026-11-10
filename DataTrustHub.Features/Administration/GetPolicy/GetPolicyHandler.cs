using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetPolicy;

public record GetPolicyQuery(Guid OrganizationId, Guid CallerId, Guid PolicyId);

// An organization admin reads a policy of their organization (C6)
public class GetPolicyHandler(IMembershipRepository memberships, IPolicyQueries policyQueries)
    : IQueryHandler<GetPolicyQuery, Result<GetPolicyResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyQueries _policyQueries = policyQueries;

    public async Task<Result<GetPolicyResponse>> Handle(
        GetPolicyQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<GetPolicyResponse>(Errors.OrganizationNotFound);

        // Scoped to the organization: a policy of another organization is not found
        var policy = await _policyQueries.FindAsync(query.OrganizationId, query.PolicyId, cancellationToken);

        return policy is null
            ? Result.Failure<GetPolicyResponse>(Errors.PolicyNotFound)
            : Result.Success(ToResponse(policy));
    }

    private static GetPolicyResponse ToResponse(PolicyEntry policy) =>
        new(policy.Id, policy.Name, policy.OrganizationId);
}
