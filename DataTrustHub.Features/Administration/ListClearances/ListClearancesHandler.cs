using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListClearances;

public record ListClearancesQuery(Guid OrganizationId, Guid CallerId, Guid? UserId, Guid? PolicyId);

// An organization admin lists the clearances on the policies of their organization (C6)
public class ListClearancesHandler(IMembershipRepository memberships, IClearanceQueries clearanceQueries)
    : IQueryHandler<ListClearancesQuery, Result<IReadOnlyList<ClearanceSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IClearanceQueries _clearanceQueries = clearanceQueries;

    public async Task<Result<IReadOnlyList<ClearanceSummary>>> Handle(
        ListClearancesQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<ClearanceSummary>>(Errors.OrganizationNotFound);

        // Scoped to the organization: a policyId of another organization gives an empty list
        var clearances = await _clearanceQueries.ListAsync(
            query.OrganizationId, query.UserId, query.PolicyId, cancellationToken);
        return Result.Success<IReadOnlyList<ClearanceSummary>>(clearances.Select(ToSummary).ToList());
    }

    private static ClearanceSummary ToSummary(ClearanceEntry clearance) =>
        new(clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId,
            clearance.ClassificationLevelName, clearance.ClassificationLevelPriority);
}
