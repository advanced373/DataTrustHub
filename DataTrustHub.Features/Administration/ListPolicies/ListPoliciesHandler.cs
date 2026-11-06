using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListPolicies;

public record ListPoliciesQuery(Guid OrganizationId, Guid CallerId);

// An organization admin lists the policies of their organization (C6)
public class ListPoliciesHandler(IMembershipRepository memberships, IPolicyQueries policyQueries)
    : IQueryHandler<ListPoliciesQuery, Result<IReadOnlyList<PolicySummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyQueries _policyQueries = policyQueries;

    public async Task<Result<IReadOnlyList<PolicySummary>>> Handle(
        ListPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<PolicySummary>>(Errors.OrganizationNotFound);

        var policies = await _policyQueries.ListAsync(query.OrganizationId, cancellationToken);
        return Result.Success<IReadOnlyList<PolicySummary>>(policies.Select(ToSummary).ToList());
    }

    private static PolicySummary ToSummary(PolicyEntry policy) =>
        new(policy.Id, policy.Name, policy.OrganizationId);
}
