using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetClearance;

public record GetClearanceQuery(Guid OrganizationId, Guid CallerId, Guid ClearanceId);

// An organization admin reads a clearance on a policy of their organization (C6)
public class GetClearanceHandler(IMembershipRepository memberships, IClearanceQueries clearanceQueries)
    : IQueryHandler<GetClearanceQuery, Result<GetClearanceResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IClearanceQueries _clearanceQueries = clearanceQueries;

    public async Task<Result<GetClearanceResponse>> Handle(
        GetClearanceQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<GetClearanceResponse>(Errors.OrganizationNotFound);

        // Scoped to the organization: a clearance on a policy of another organization is not found
        var clearance = await _clearanceQueries.FindAsync(
            query.OrganizationId, query.ClearanceId, cancellationToken);

        return clearance is null
            ? Result.Failure<GetClearanceResponse>(Errors.ClearanceNotFound)
            : Result.Success(ToResponse(clearance));
    }

    private static GetClearanceResponse ToResponse(ClearanceEntry clearance) =>
        new(clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId,
            clearance.ClassificationLevelName, clearance.ClassificationLevelPriority);
}
